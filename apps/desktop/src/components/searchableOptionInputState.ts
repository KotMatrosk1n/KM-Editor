/* SPDX-License-Identifier: GPL-3.0-only */

export type SearchableOption = Readonly<{
  label: string;
  value: number;
}>;

export type SearchableOptionInteractionState = Readonly<{
  hasUserQuery: boolean;
  isOpen: boolean;
  query: string;
}>;

export type SearchableOptionInteractionEvent =
  | Readonly<{ formattedValue: string; type: 'focus' }>
  | Readonly<{ query: string; type: 'input' }>
  | Readonly<{
      committedValue: string;
      emptyOptionLabel?: string;
      formattedValue: string;
      options: readonly SearchableOption[];
      type: 'commit';
    }>
  | Readonly<{ formattedValue: string; type: 'restore' }>;

export type SearchableOptionInteractionResult = Readonly<{
  /**
   * A source update is permitted only when this is non-null. In particular,
   * ambiguous text and a required blank restore the committed display value
   * without ever sending a partial value to the source editor.
   */
  sourceCommit: Readonly<{ value: string }> | null;
  state: SearchableOptionInteractionState;
}>;

export function createSearchableOptionInteractionState(
  formattedValue: string
): SearchableOptionInteractionState {
  return {
    hasUserQuery: false,
    isOpen: false,
    query: formattedValue
  };
}

export function transitionSearchableOptionInteraction(
  state: SearchableOptionInteractionState,
  event: SearchableOptionInteractionEvent
): SearchableOptionInteractionResult {
  if (event.type === 'focus') {
    return {
      sourceCommit: null,
      state: {
        hasUserQuery: false,
        isOpen: true,
        query: event.formattedValue
      }
    };
  }

  if (event.type === 'input') {
    return {
      sourceCommit: null,
      state: {
        hasUserQuery: true,
        isOpen: true,
        query: event.query
      }
    };
  }

  if (event.type === 'restore' || !state.hasUserQuery) {
    return {
      sourceCommit: null,
      state: createSearchableOptionInteractionState(event.formattedValue)
    };
  }

  const resolvedValue = resolveSearchableOptionCommit(
    state.query,
    event.options,
    event.emptyOptionLabel
  );
  if (resolvedValue === null) {
    return {
      sourceCommit: null,
      state: createSearchableOptionInteractionState(event.formattedValue)
    };
  }

  const nextQuery = resolvedValue.length === 0
    ? event.emptyOptionLabel ?? ''
    : formatSearchableOptionValue(resolvedValue, event.options, event.emptyOptionLabel);
  return {
    sourceCommit:
      resolvedValue === event.committedValue.trim()
        ? null
        : { value: resolvedValue },
    state: createSearchableOptionInteractionState(nextQuery)
  };
}

export function findExactSearchableOption(
  value: string,
  options: readonly SearchableOption[]
) {
  const trimmedValue = value.trim();
  const normalizedValue = trimmedValue.toLocaleLowerCase();
  return options.find(
    (option) =>
      option.value.toString() === trimmedValue ||
      option.label.toLocaleLowerCase() === normalizedValue
  );
}

export function resolveSearchableOptionCommit(
  value: string,
  options: readonly SearchableOption[],
  emptyOptionLabel?: string
): string | null {
  const trimmedValue = value.trim();
  if (trimmedValue.length === 0) {
    return emptyOptionLabel === undefined ? null : '';
  }

  if (
    emptyOptionLabel !== undefined &&
    trimmedValue.toLocaleLowerCase() === emptyOptionLabel.toLocaleLowerCase()
  ) {
    return '';
  }

  const exactOption = findExactSearchableOption(trimmedValue, options);
  if (exactOption) {
    return exactOption.value.toString();
  }

  if (/^[+-]?\d+$/u.test(trimmedValue)) {
    const numericValue = Number(trimmedValue);
    if (Number.isSafeInteger(numericValue)) {
      return numericValue.toString();
    }
  }

  const smartMatches = getSmartOptionMatches(trimmedValue, options);
  return smartMatches.length === 1 ? smartMatches[0]!.value.toString() : null;
}

export function formatSearchableOptionValue(
  value: string,
  options: readonly SearchableOption[],
  emptyOptionLabel?: string
) {
  const trimmedValue = value.trim();
  if (trimmedValue.length === 0) {
    return emptyOptionLabel ?? value;
  }

  return (
    options.find((option) => option.value.toString() === trimmedValue)?.label ??
    options.find((option) => option.label === value)?.label ??
    value
  );
}

export function getSmartOptionMatches<T extends SearchableOption>(
  value: string,
  options: readonly T[]
): T[] {
  const query = value.trim();
  if (query.length === 0) {
    return [...options];
  }

  const normalizedQuery = query.toLocaleLowerCase();
  const numericPrefix = normalizedQuery.match(/^\d+/)?.[0] ?? null;
  const letterPrefix = normalizedQuery.match(/^[a-z]+/)?.[0] ?? null;

  if (numericPrefix) {
    const normalizedNumericPrefix = numericPrefix.replace(/^0+/, '') || '0';

    return options
      .filter((option) => {
        const rawValue = option.value.toString();
        const normalizedValue = rawValue.replace(/^0+/, '') || '0';
        const labelNumericPrefix =
          option.label.match(/^\s*\$?\s*0*([\d,]+)/)?.[1]?.replace(/,/g, '') ?? null;

        return (
          rawValue.startsWith(numericPrefix) ||
          normalizedValue.startsWith(normalizedNumericPrefix) ||
          labelNumericPrefix?.startsWith(normalizedNumericPrefix)
        );
      })
      .slice(0, 100);
  }

  if (letterPrefix) {
    return options
      .filter((option) =>
        option.label
          .toLocaleLowerCase()
          .split(/[^a-z0-9]+/)
          .some((token) => token.startsWith(letterPrefix))
      )
      .slice(0, 100);
  }

  return options
    .filter((option) => option.label.toLocaleLowerCase().startsWith(normalizedQuery))
    .slice(0, 100);
}
