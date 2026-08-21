/* SPDX-License-Identifier: GPL-3.0-only */

export type GroupedDiagnostic<T> = {
  count: number;
  diagnostic: T;
  key: string;
};

export type PresentedDiagnosticGroup<T> = {
  count: number;
  diagnostics: GroupedDiagnostic<T>[];
  key: string;
};

export function groupDiagnosticsForPresentation<T>(
  diagnostics: readonly T[],
  visibleIdentity: (diagnostic: T) => readonly (string | null | undefined)[],
  technicalIdentity: (diagnostic: T) => readonly (string | null | undefined)[]
): PresentedDiagnosticGroup<T>[] {
  const groups = new Map<string, {
    count: number;
    diagnostics: Map<string, GroupedDiagnostic<T>>;
    key: string;
  }>();
  for (const diagnostic of diagnostics) {
    const key = normalizedIdentity(visibleIdentity(diagnostic));
    let group = groups.get(key);
    if (!group) {
      group = { count: 0, diagnostics: new Map(), key };
      groups.set(key, group);
    }
    group.count += 1;
    const technicalKey = normalizedIdentity(technicalIdentity(diagnostic));
    const existing = group.diagnostics.get(technicalKey);
    if (existing) existing.count += 1;
    else group.diagnostics.set(technicalKey, { count: 1, diagnostic, key: technicalKey });
  }
  return [...groups.values()].map((group) => ({
    count: group.count,
    diagnostics: [...group.diagnostics.values()],
    key: group.key
  }));
}

function normalizedIdentity(values: readonly (string | null | undefined)[]) {
  return JSON.stringify(values.map((value) => (
    typeof value === 'string' ? value.trim().replace(/\s+/gu, ' ') : value ?? null
  )));
}

export function humanizeIdentifier(value: string) {
  const normalized = value
    .replace(/([a-z0-9])([A-Z])/gu, '$1 $2')
    .replace(/[._:/-]+/gu, ' ')
    .replace(/\s+/gu, ' ')
    .trim();
  if (!normalized) return value;
  return normalized.charAt(0).toLocaleUpperCase() + normalized.slice(1);
}

export function presentationFactLabelKey(label: string) {
  switch (label.trim().toLocaleLowerCase()) {
    case 'species id':
      return 'analysisPresentation.label.speciesNumber';
    case 'trainer id':
      return 'analysisPresentation.label.trainerNumber';
    case 'item id':
      return 'analysisPresentation.label.itemNumber';
    case 'move id':
      return 'analysisPresentation.label.moveNumber';
    case 'held item id':
      return 'analysisPresentation.label.heldItem';
    default:
      return null;
  }
}

export function relativeRecordTitle(title: string, parentTitle: string) {
  const prefixes = [`${parentTitle} - `, `${parentTitle}, `, `${parentTitle}: `];
  const prefix = prefixes.find((candidate) => title.startsWith(candidate));
  const relative = prefix ? title.slice(prefix.length) : title;
  return relative.charAt(0).toLocaleUpperCase() + relative.slice(1);
}

export type PresentedFactValue = {
  changed: boolean;
  displayValue: string;
  exactValue: string;
  unit: string | null;
};

export function presentFactValue(
  label: string,
  displayValue: string,
  unit: string | null,
  translateLiteral: (literal: string) => string
): PresentedFactValue {
  const normalizedLabel = label.trim().toLocaleLowerCase();
  const normalizedValue = displayValue.trim().toLocaleLowerCase();
  let friendlyValue = displayValue;
  let friendlyUnit = unit;

  if (normalizedValue === 'true') {
    friendlyValue = translateLiteral('Yes');
    friendlyUnit = null;
  } else if (normalizedValue === 'false') {
    friendlyValue = translateLiteral('No');
    friendlyUnit = null;
  } else if (normalizedValue === '0' && normalizedLabel === 'form') {
    friendlyValue = translateLiteral('Default');
    friendlyUnit = null;
  } else if (
    normalizedValue === '0' &&
    (normalizedLabel === 'held item id' || normalizedLabel === 'move count')
  ) {
    friendlyValue = translateLiteral('None');
    friendlyUnit = null;
  }

  const exactValue = unit ? `${displayValue} ${unit}` : displayValue;
  const friendlyExact = friendlyUnit ? `${friendlyValue} ${friendlyUnit}` : friendlyValue;
  return {
    changed: friendlyExact !== exactValue,
    displayValue: friendlyValue,
    exactValue,
    unit: friendlyUnit
  };
}
