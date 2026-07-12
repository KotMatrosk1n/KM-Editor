/* SPDX-License-Identifier: GPL-3.0-only */

import type {
  ItemsWorkflow,
  PokemonEvolutionMethodOption,
  PokemonEvolutionRecord,
  PokemonRecord,
  PokemonWorkflow
} from '../../bridge/contracts';

export type PokemonGroupedPendingEditDetails = {
  action: string;
  slot: number;
};

export type PokemonEvolutionPendingEditContext = {
  itemsWorkflow: ItemsWorkflow | null;
  pokemon: PokemonRecord | undefined;
  pokemonWorkflow: PokemonWorkflow | null;
};

export function formatPokemonEvolutionPendingValue(
  value: string | null | undefined,
  details: PokemonGroupedPendingEditDetails | null,
  context: PokemonEvolutionPendingEditContext
) {
  if (!details) {
    return formatPendingValue(value);
  }

  switch (details.action) {
    case 'upsert': {
      const [methodText, argumentText, speciesText, formText, levelText] =
        splitPokemonOperationValue(value);
      const methodId = parseOptionalInteger(methodText);
      const argumentId = parseOptionalInteger(argumentText);
      const methodOption = context.pokemonWorkflow?.evolutionMethodOptions.find(
        (option) => option.value === methodId
      );
      const evolution = context.pokemon?.evolutions.find(
        (candidate) =>
          candidate.slot === details.slot &&
          candidate.method === methodId &&
          candidate.argument === argumentId
      );
      const method = formatPendingOptionValue(
        methodText,
        context.pokemonWorkflow?.evolutionMethodOptions
      );
      const argument = formatPokemonEvolutionArgument(
        argumentText,
        methodOption,
        evolution,
        context
      );
      const species = formatPokemonSpeciesPendingValue(speciesText, context.pokemonWorkflow);

      return [
        method,
        argument,
        species,
        formText ? `Form ${formText}` : null,
        levelText ? `Lv. ${levelText}` : null
      ]
        .filter((part): part is string => part !== null && part.length > 0)
        .join(' / ') || formatPendingValue(value);
    }
    case 'moveUp':
      return `Move slot #${details.slot + 1} up`;
    case 'moveDown':
      return `Move slot #${details.slot + 1} down`;
    case 'remove':
      return `Remove slot #${details.slot + 1}`;
    default:
      return formatPendingValue(value);
  }
}

function formatPokemonEvolutionArgument(
  value: string | undefined,
  methodOption: PokemonEvolutionMethodOption | undefined,
  evolution: PokemonEvolutionRecord | undefined,
  context: PokemonEvolutionPendingEditContext
) {
  const argumentKind = methodOption?.argumentKind ?? evolution?.argumentKind;
  if (argumentKind === 'none' || argumentKind === 'level') {
    return null;
  }

  if (!value) {
    return null;
  }

  const argumentId = parseOptionalInteger(value);
  const argumentLabel = methodOption?.argumentLabel ?? evolution?.argumentLabel ?? 'Argument';
  let option = findPendingOption(methodOption?.argumentOptions, value);
  if (!option && argumentKind === 'item') {
    option = context.pokemonWorkflow?.evolutionMethodOptions
      .filter((candidate) => candidate.argumentKind === 'item')
      .flatMap((candidate) => candidate.argumentOptions)
      .find((candidate) => candidate.value === argumentId);
  }

  let formattedValue = option?.label;
  if (!formattedValue && evolution?.argumentValue) {
    formattedValue = evolution.argumentValue;
  }

  if (!formattedValue && argumentKind === 'item' && argumentId !== null) {
    const item = context.itemsWorkflow?.items.find((candidate) => candidate.itemId === argumentId);
    formattedValue = item ? `${argumentId} ${item.name}` : argumentId.toString();
  }

  return `${argumentLabel} ${formattedValue ?? value}`;
}

function formatPokemonSpeciesPendingValue(
  value: string | undefined,
  workflow: PokemonWorkflow | null
) {
  const speciesId = parseOptionalInteger(value);
  if (speciesId === null) {
    return formatPendingValue(value);
  }

  const pokemon = workflow?.pokemon.find((candidate) => candidate.speciesId === speciesId);
  return pokemon ? `${speciesId.toString().padStart(3, '0')} ${pokemon.name}` : value ?? 'n/a';
}

function formatPendingOptionValue(
  value: string | undefined,
  options: readonly { label: string; value: number }[] | undefined
) {
  if (!value) {
    return 'n/a';
  }

  return findPendingOption(options, value)?.label ?? value;
}

function findPendingOption<TOption extends { label: string; value: number }>(
  options: readonly TOption[] | undefined,
  value: string
) {
  const parsedValue = parseOptionalInteger(value);
  return parsedValue === null
    ? undefined
    : options?.find((option) => option.value === parsedValue);
}

function splitPokemonOperationValue(value: string | null | undefined) {
  const text = value ?? '';
  return text.includes('|') ? text.split('|') : text.split(':');
}

function parseOptionalInteger(value: string | null | undefined) {
  const text = value?.trim();
  if (!text || !/^-?\d+$/.test(text)) {
    return null;
  }

  const parsedValue = Number.parseInt(text, 10);
  return Number.isSafeInteger(parsedValue) ? parsedValue : null;
}

function formatPendingValue(value: string | null | undefined) {
  return value ? value : 'n/a';
}
