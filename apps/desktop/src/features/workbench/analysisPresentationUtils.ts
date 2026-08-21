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
  technicalIdentity: (diagnostic: T) => readonly (string | null | undefined)[],
  priority: (diagnostic: T) => number = () => 0
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
  return [...groups.values()]
    .map((group) => ({
      count: group.count,
      diagnostics: [...group.diagnostics.values()].sort((left, right) => (
        priority(right.diagnostic) - priority(left.diagnostic) || left.key.localeCompare(right.key)
      )),
      key: group.key
    }))
    .sort((left, right) => (
      priority(right.diagnostics[0]!.diagnostic) - priority(left.diagnostics[0]!.diagnostic)
    ));
}

type PresentableDiagnostic = {
  code?: string | null;
  domain?: string | null;
  field?: string | null;
  message: string;
  severity: 'error' | 'warning' | 'info';
};

const owningWorkflowSummaryMessages = new Set([
  'The owning workflow reported a diagnostic while preparing this read-only analysis.',
  'The owning workflow reported a diagnostic while preparing this read-only module.'
]);

export function diagnosticSeverityPriority(severity: PresentableDiagnostic['severity']) {
  switch (severity) {
    case 'error':
      return 3;
    case 'warning':
      return 2;
    case 'info':
      return 1;
  }
}

export function presentationDiagnosticMessage<T extends PresentableDiagnostic>(
  diagnostic: T,
  diagnostics: readonly T[],
  formatMessage: (value: T) => string
) {
  const candidates = presentationDiagnosticCandidates(diagnostic, diagnostics, formatMessage);
  return candidates
    ? formatMessage(candidates[0]!)
    : formatMessage(diagnostic);
}

export function presentationDiagnosticSeverity<T extends PresentableDiagnostic>(
  diagnostic: T,
  diagnostics: readonly T[],
  formatMessage: (value: T) => string
) {
  const candidates = presentationDiagnosticCandidates(diagnostic, diagnostics, formatMessage);
  return candidates
    ? [...candidates].sort((left, right) => (
        diagnosticSeverityPriority(right.severity) - diagnosticSeverityPriority(left.severity)
      ))[0]!.severity
    : diagnostic.severity;
}

function presentationDiagnosticCandidates<T extends PresentableDiagnostic>(
  diagnostic: T,
  diagnostics: readonly T[],
  formatMessage: (value: T) => string
) {
  if (
    diagnostic.code ||
    diagnostic.field ||
    !owningWorkflowSummaryMessages.has(diagnostic.message.trim())
  ) return null;

  const candidates = diagnostics.filter((candidate) => (
    candidate.domain === diagnostic.domain &&
    owningWorkflowSummaryMessages.has(candidate.message.trim()) &&
    (candidate.code || candidate.field)
  ));
  const actionableMessages = new Set(candidates.map(formatMessage));
  return actionableMessages.size === 1 ? candidates : null;
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
