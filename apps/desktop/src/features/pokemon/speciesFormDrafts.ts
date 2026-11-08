/* SPDX-License-Identifier: GPL-3.0-only */

import type { PokemonRecord } from '../../bridge/contracts';

export type SpeciesFormGameFamily = 'swsh' | 'sv' | 'za';

// Sword and Shield encounter forms are indices in the base species' personal table range.
export function getSwShPokemonFormPersonalId(
  speciesId: number,
  form: number,
  baseRecord?: PokemonRecord
) {
  if (form === 0) return baseRecord?.personalId ?? speciesId;

  const personal = baseRecord?.personal;
  return personal && personal.formStatsIndex > 0 && form > 0 && form < personal.formCount
    ? personal.formStatsIndex + form - 1
    : null;
}

export function findSwShPokemonFormRecord(
  pokemon: readonly PokemonRecord[],
  speciesId: number,
  form: number
) {
  const baseRecord = pokemon.find(record => record.speciesId === speciesId && record.personalId === speciesId)
    ?? pokemon.find(record => record.speciesId === speciesId && record.form === 0);
  if (form === 0) return baseRecord;

  const personalId = getSwShPokemonFormPersonalId(speciesId, form, baseRecord);
  return pokemon.find(record => record.personalId === personalId && record.speciesId === speciesId);
}

export type SpeciesFormCatalogOption = {
  formOptions?: readonly SpeciesFormCatalogOption[] | null;
  value: number;
};

export type SpeciesFormCatalogField = {
  options?: readonly SpeciesFormCatalogOption[];
};

type SpeciesChangeFormResolution = {
  gameFamily: SpeciesFormGameFamily;
  pokemonRecords?: readonly PokemonRecord[] | null;
  previousForm: number;
  sourceForm: number;
  sourceSpeciesId: number | null;
  speciesField?: SpeciesFormCatalogField | null;
  targetSpeciesId: number;
};

export function resolveSpeciesChangeForm({
  gameFamily,
  pokemonRecords,
  previousForm,
  sourceForm,
  sourceSpeciesId,
  speciesField,
  targetSpeciesId
}: SpeciesChangeFormResolution): number | null {
  if (targetSpeciesId === 0) {
    return 0;
  }

  if (sourceSpeciesId !== null && targetSpeciesId === sourceSpeciesId) {
    return sourceForm;
  }

  const availableForms = getAvailableSpeciesForms(
    targetSpeciesId,
    gameFamily,
    speciesField,
    pokemonRecords
  );
  if (availableForms === null || availableForms.length === 0) {
    return null;
  }

  if (availableForms.includes(previousForm)) {
    return previousForm;
  }

  return availableForms.includes(0) ? 0 : availableForms[0]!;
}

export function getAvailableSpeciesForms(
  speciesId: number,
  gameFamily: SpeciesFormGameFamily,
  speciesField?: SpeciesFormCatalogField | null,
  pokemonRecords?: readonly PokemonRecord[] | null
): number[] | null {
  if (speciesId === 0) {
    return [0];
  }

  const selectedSpeciesOption = speciesField?.options?.find(
    (option) => option.value === speciesId
  );
  if (
    selectedSpeciesOption?.formOptions !== undefined &&
    selectedSpeciesOption.formOptions !== null
  ) {
    return normalizeFormValues(selectedSpeciesOption.formOptions.map((option) => option.value));
  }

  if (pokemonRecords && pokemonRecords.length > 0) {
    const presentRecords = pokemonRecords.filter(
      (record) => record.speciesId === speciesId && record.personal.isPresentInGame
    );

    if (gameFamily === 'swsh') {
      const baseRecord = findSwShPokemonFormRecord(presentRecords, speciesId, 0);
      if (!baseRecord) {
        return null;
      }

      const formCount = Math.max(1, baseRecord.personal.formCount);
      return Array.from({ length: formCount }, (_, form) => form);
    }

    return normalizeFormValues(presentRecords.map((record) => record.form));
  }

  // Z-A form IDs are sparse and come from exact present Personal rows. If that
  // catalog is unavailable, leave the draft unresolved so backend validation
  // can fail closed instead of fabricating a pair.
  return gameFamily === 'za' ? null : [0];
}

function normalizeFormValues(values: readonly number[]) {
  return [
    ...new Set(values.filter((value) => Number.isInteger(value) && value >= 0))
  ].sort((left, right) => left - right);
}
