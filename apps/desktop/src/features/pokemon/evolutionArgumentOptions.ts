/* SPDX-License-Identifier: GPL-3.0-only */

import type {
  PokemonEvolutionMethodOption,
  PokemonEvolutionRecord
} from '../../bridge/contracts';

export function evolutionArgumentUsesGameText(kind: string | undefined) {
  return kind === 'item' || kind === 'move' || kind === 'species';
}

export function createEvolutionArgumentOptions(
  method: PokemonEvolutionMethodOption | null,
  draft: string,
  methods: readonly PokemonEvolutionMethodOption[],
  evolution: PokemonEvolutionRecord | null,
  fallbackLabel: string
) {
  const options = method?.argumentOptions ?? [];
  if (!/^-?\d+$/u.test(draft.trim())) {
    return options;
  }

  const value = Number(draft);
  if (!Number.isSafeInteger(value) || options.some(option => option.value === value)) {
    return options;
  }

  // Use-item methods have filtered catalogs. Other methods can still name the same item.
  const usesGameText = evolutionArgumentUsesGameText(method?.argumentKind);
  const knownOption = usesGameText
    ? methods
      .find(candidate => candidate.argumentKind === method?.argumentKind &&
        candidate.argumentOptions.some(option => option.value === value))
      ?.argumentOptions.find(option => option.value === value)
    : undefined;
  const recordName = evolution?.argumentKind === method?.argumentKind &&
    evolution?.argument === value && (usesGameText || evolution.method === method?.value)
    ? evolution.argumentValue
    : undefined;

  return [...options, knownOption ?? {
    value,
    label: `${value} ${recordName || fallbackLabel}`
  }];
}
