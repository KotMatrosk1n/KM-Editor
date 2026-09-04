/* SPDX-License-Identifier: GPL-3.0-only */

export type SearchableOption = Readonly<{
  inputLabel?: string;
  label: string;
  searchAliases?: readonly string[];
  value: string | number;
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
      isFiniteCatalog?: boolean;
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
    event.emptyOptionLabel,
    event.isFiniteCatalog
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
  const valueMatch = options.find(
    (option) => option.value.toString() === trimmedValue
  );
  if (valueMatch) {
    return valueMatch;
  }

  const labelMatches = options.filter(
    (option) =>
      option.label.toLocaleLowerCase() === normalizedValue ||
      option.inputLabel?.toLocaleLowerCase() === normalizedValue
  );
  if (labelMatches.length > 0) {
    const distinctValues = new Set(labelMatches.map((option) => option.value.toString()));
    return distinctValues.size === 1 ? labelMatches[0] : undefined;
  }

  const aliasMatches = options.filter((option) =>
    option.searchAliases?.some(
      (alias) => alias.trim().toLocaleLowerCase() === normalizedValue
    )
  );
  return aliasMatches.length === 1 ? aliasMatches[0] : undefined;
}

export function resolveSearchableOptionCommit(
  value: string,
  options: readonly SearchableOption[],
  emptyOptionLabel?: string,
  isFiniteCatalog = false
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

  if (!isFiniteCatalog && /^[+-]?\d+$/u.test(trimmedValue)) {
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

  const option =
    options.find((candidate) => candidate.value.toString() === trimmedValue) ??
    options.find((candidate) => candidate.label === value || candidate.inputLabel === value);
  return option?.inputLabel ?? option?.label ?? value;
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

  if (numericPrefix) {
    const normalizedNumericPrefix = numericPrefix.replace(/^0+/, '') || '0';

    return options
      .filter((option) => {
        const rawValue = option.value.toString();
        const normalizedValue = rawValue.replace(/^0+/, '') || '0';
        const searchableNumericPrefixes = [
          option.label,
          option.inputLabel,
          ...(option.searchAliases ?? [])
        ]
          .filter((label): label is string => label !== undefined)
          .map(
            (label) =>
              label.match(/^\s*\$?\s*0*([\d,]+)/)?.[1]?.replace(/,/g, '') ?? null
          );

        return (
          rawValue.startsWith(numericPrefix) ||
          normalizedValue.startsWith(normalizedNumericPrefix) ||
          searchableNumericPrefixes.some((prefix) =>
            prefix?.startsWith(normalizedNumericPrefix)
          )
        );
      })
      .slice(0, 100);
  }

  const queryTokens = normalizedQuery.split(/[^a-z0-9]+/u).filter(Boolean);
  if (queryTokens.length > 0) {
    return options
      .filter((option) => {
        const optionTokens = [option.label, option.inputLabel, ...(option.searchAliases ?? [])]
          .filter((label): label is string => label !== undefined)
          .flatMap((label) => label.toLocaleLowerCase().split(/[^a-z0-9]+/u))
          .filter(Boolean);
        return queryTokens.every((queryToken) =>
          optionTokens.some((optionToken) => optionToken.startsWith(queryToken))
        );
      })
      .slice(0, 100);
  }

  return options
    .filter((option) =>
      [option.label, option.inputLabel, ...(option.searchAliases ?? [])].some((label) =>
        label?.toLocaleLowerCase().startsWith(normalizedQuery)
      )
    )
    .slice(0, 100);
}
