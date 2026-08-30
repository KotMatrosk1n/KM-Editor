/* SPDX-License-Identifier: GPL-3.0-only */

export function parseBoundedWholeNumberDraft(
  value: string,
  minimum: number,
  maximum: number
) {
  if (
    !Number.isSafeInteger(minimum) ||
    !Number.isSafeInteger(maximum) ||
    minimum > maximum ||
    !/^\d+$/u.test(value)
  ) {
    return null;
  }

  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed >= minimum && parsed <= maximum
    ? parsed
    : null;
}

export function findExactSearchableOption<TOption>(
  options: readonly TOption[],
  query: string,
  getId: (option: TOption) => number,
  getLabel: (option: TOption) => string
) {
  const normalized = normalizeSearchQuery(query);
  if (!normalized) {
    return null;
  }

  const exactId = options.find((option) => getId(option).toString() === normalized);
  if (exactId) {
    return exactId;
  }

  const exactLabels = options.filter(
    (option) => normalizeSearchQuery(getLabel(option)) === normalized
  );
  return exactLabels.length === 1 ? exactLabels[0] ?? null : null;
}

export function filterAndRankSearchableOptions<TOption>(
  options: readonly TOption[],
  query: string,
  maximumResults: number,
  getId: (option: TOption) => number,
  getLabel: (option: TOption) => string,
  getSearchValues: (option: TOption) => readonly string[]
) {
  if (!Number.isSafeInteger(maximumResults) || maximumResults < 1) {
    return [];
  }

  const normalized = normalizeSearchQuery(query);
  const matches = normalized.length === 0
    ? [...options]
    : options.filter((option) =>
      getSearchValues(option).some((value) =>
        normalizeSearchQuery(value).includes(normalized)
      )
    );
  const exact = findExactSearchableOption(options, query, getId, getLabel);
  if (exact) {
    const exactId = getId(exact);
    const exactIndex = matches.findIndex((option) => getId(option) === exactId);
    if (exactIndex > 0) {
      matches.splice(exactIndex, 1);
      matches.unshift(exact);
    } else if (exactIndex < 0) {
      matches.unshift(exact);
    }
  }
  return matches.slice(0, maximumResults);
}

function normalizeSearchQuery(value: string) {
  return value.trim().toLocaleLowerCase();
}
