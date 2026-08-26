/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectDraftAdapter } from './draftRegistry';
import type { JsonValue } from './semanticContracts';

export type OrdinaryFieldDraftPayload = {
  fields: Record<string, string>;
};

export type OrdinaryPokemonDraftPayload = {
  alphaMove: string | null;
  evolutionSlots: Record<
    string,
    {
      argument: string;
      form: string;
      level: string;
      method: string;
      species: string;
    }
  >;
  fields: Record<string, string>;
  learnsetSlots: Record<string, { level: string; moveId: string }>;
};

export type OrdinaryTextDraftPayload = {
  value: string;
};

export type OrdinaryTrainerDraftPayload = {
  partyBySlot: Record<string, Record<string, string>>;
  trainerFields: Record<string, string>;
};

export const ordinaryItemDraftAdapter: ProjectDraftAdapter<OrdinaryFieldDraftPayload> =
  createFieldDraftAdapter('ordinary-editor.items');

export const ordinaryMoveDraftAdapter: ProjectDraftAdapter<OrdinaryFieldDraftPayload> =
  createFieldDraftAdapter('ordinary-editor.moves');

export const ordinaryPokemonDraftAdapter: ProjectDraftAdapter<OrdinaryPokemonDraftPayload> = {
  adapterId: 'ordinary-editor.pokemon',
  parsePayload: (payload) => {
    const value = requireExactObject(payload, [
      'alphaMove',
      'evolutionSlots',
      'fields',
      'learnsetSlots'
    ]);
    if (value.alphaMove !== null && typeof value.alphaMove !== 'string') {
      throw new Error('Pokemon alpha move draft is invalid.');
    }
    return {
      alphaMove: value.alphaMove,
      evolutionSlots: parseEvolutionSlots(value.evolutionSlots),
      fields: parseStringRecord(value.fields),
      learnsetSlots: parseLearnsetSlots(value.learnsetSlots)
    };
  },
  schemaVersion: 1,
  serializePayload: (draft) => ({
    alphaMove: draft.alphaMove,
    evolutionSlots: sortNestedStringRecord(draft.evolutionSlots),
    fields: sortStringRecord(draft.fields),
    learnsetSlots: sortNestedStringRecord(draft.learnsetSlots)
  })
};

export const ordinaryTextDraftAdapter: ProjectDraftAdapter<OrdinaryTextDraftPayload> = {
  adapterId: 'ordinary-editor.text',
  parsePayload: (payload) => {
    const value = requireExactObject(payload, ['value']);
    if (typeof value.value !== 'string') {
      throw new Error('Text draft value is invalid.');
    }
    return { value: value.value };
  },
  schemaVersion: 1,
  serializePayload: (draft) => ({ value: draft.value })
};

export const ordinaryTrainerDraftAdapter: ProjectDraftAdapter<OrdinaryTrainerDraftPayload> = {
  adapterId: 'ordinary-editor.trainers',
  parsePayload: (payload) => {
    const value = requireExactObject(payload, ['partyBySlot', 'trainerFields']);
    return {
      partyBySlot: parseNestedStringRecord(value.partyBySlot),
      trainerFields: parseStringRecord(value.trainerFields)
    };
  },
  schemaVersion: 1,
  serializePayload: (draft) => ({
    partyBySlot: sortNestedStringRecord(draft.partyBySlot),
    trainerFields: sortStringRecord(draft.trainerFields)
  })
};

export function isOrdinaryFieldDraftClean(payload: OrdinaryFieldDraftPayload) {
  return Object.keys(payload.fields).length === 0;
}

export function isOrdinaryPokemonDraftClean(payload: OrdinaryPokemonDraftPayload) {
  return (
    payload.alphaMove === null &&
    Object.keys(payload.evolutionSlots).length === 0 &&
    Object.keys(payload.fields).length === 0 &&
    Object.keys(payload.learnsetSlots).length === 0
  );
}

export function isOrdinaryTrainerDraftClean(payload: OrdinaryTrainerDraftPayload) {
  return (
    Object.keys(payload.partyBySlot).length === 0 &&
    Object.keys(payload.trainerFields).length === 0
  );
}

function createFieldDraftAdapter(
  adapterId: string
): ProjectDraftAdapter<OrdinaryFieldDraftPayload> {
  return {
    adapterId,
    parsePayload: (payload) => {
      const value = requireExactObject(payload, ['fields']);
      return { fields: parseStringRecord(value.fields) };
    },
    schemaVersion: 1,
    serializePayload: (draft) => ({ fields: sortStringRecord(draft.fields) })
  };
}

function parseEvolutionSlots(value: JsonValue | undefined) {
  const record = requireObject(value);
  return Object.fromEntries(
    Object.entries(record).map(([slot, entry]) => {
      requireSlotKey(slot);
      const fields = requireExactObject(entry, [
        'argument',
        'form',
        'level',
        'method',
        'species'
      ]);
      return [
        slot,
        {
          argument: requireString(fields.argument),
          form: requireString(fields.form),
          level: requireString(fields.level),
          method: requireString(fields.method),
          species: requireString(fields.species)
        }
      ];
    })
  );
}

function parseLearnsetSlots(value: JsonValue | undefined) {
  const record = requireObject(value);
  return Object.fromEntries(
    Object.entries(record).map(([slot, entry]) => {
      requireSlotKey(slot);
      const fields = requireExactObject(entry, ['level', 'moveId']);
      return [
        slot,
        {
          level: requireString(fields.level),
          moveId: requireString(fields.moveId)
        }
      ];
    })
  );
}

function parseNestedStringRecord(value: JsonValue | undefined) {
  const record = requireObject(value);
  return Object.fromEntries(
    Object.entries(record).map(([key, entry]) => {
      requireStableKey(key);
      return [key, parseStringRecord(entry)];
    })
  );
}

function parseStringRecord(value: JsonValue | undefined) {
  const record = requireObject(value);
  return Object.fromEntries(
    Object.entries(record).map(([key, entry]) => {
      requireStableKey(key);
      return [key, requireString(entry)];
    })
  );
}

function requireExactObject(value: JsonValue | undefined, keys: readonly string[]) {
  const record = requireObject(value);
  const actualKeys = Object.keys(record).sort();
  const expectedKeys = [...keys].sort();
  if (
    actualKeys.length !== expectedKeys.length ||
    actualKeys.some((key, index) => key !== expectedKeys[index])
  ) {
    throw new Error('Draft payload shape is invalid.');
  }
  return record;
}

function requireObject(value: JsonValue | undefined): Record<string, JsonValue> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new Error('Draft payload object is invalid.');
  }
  return value;
}

function requireString(value: JsonValue | undefined) {
  if (typeof value !== 'string') {
    throw new Error('Draft payload string is invalid.');
  }
  return value;
}

function requireSlotKey(value: string) {
  if (!/^(?:0|[1-9][0-9]*)$/u.test(value) || !Number.isSafeInteger(Number(value))) {
    throw new Error('Draft slot key is invalid.');
  }
}

function requireStableKey(value: string) {
  if (
    value.length === 0 ||
    value.length > 512 ||
    value.trim() !== value ||
    /[\u0000-\u001f\u007f-\u009f]/u.test(value)
  ) {
    throw new Error('Draft field key is invalid.');
  }
}

function sortStringRecord(value: Record<string, string>) {
  return Object.fromEntries(
    Object.entries(value).sort(([left], [right]) => left.localeCompare(right))
  );
}

function sortNestedStringRecord<TValue extends Record<string, string>>(
  value: Record<string, TValue>
) {
  return Object.fromEntries(
    Object.entries(value)
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([key, entry]) => [key, sortStringRecord(entry)])
  );
}
