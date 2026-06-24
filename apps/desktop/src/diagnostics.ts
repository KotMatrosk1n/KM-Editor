/* SPDX-License-Identifier: GPL-3.0-only */

import type { ApiDiagnostic } from './bridge/contracts';

type DiagnosticTranslator = (literal: string) => string;

const identityTranslator: DiagnosticTranslator = (literal) => literal;

export function formatDiagnosticMessage(
  diagnostic: ApiDiagnostic,
  translateLiteral: DiagnosticTranslator = identityTranslator
) {
  const message = normalizeSentence(translateLiteral(diagnostic.message));

  if (diagnostic.severity === 'info') {
    return message;
  }

  const valueDetails = [
    formatLabeledDetail('File', diagnostic.file, translateLiteral),
    formatLabeledDetail('Field', formatFieldName(diagnostic.field), translateLiteral),
    formatLabeledDetail('Expected', diagnostic.expected, translateLiteral)
  ].filter((detail): detail is string => detail !== null);

  if (valueDetails.length === 0) {
    return message;
  }

  const details = [
    formatDomainDetail(diagnostic.domain, translateLiteral),
    ...valueDetails
  ].filter((detail): detail is string => detail !== null);

  return `${message} ${details.join(' ')}`;
}

function formatDomainDetail(
  domain: string | null | undefined,
  translateLiteral: DiagnosticTranslator
) {
  if (!domain) {
    return null;
  }

  const readableDomain = formatDomainName(domain);

  return readableDomain
    ? `${translateLiteral('Area')}: ${translateLiteral(readableDomain)}.`
    : null;
}

function formatLabeledDetail(
  label: string,
  value: string | null | undefined,
  translateLiteral: DiagnosticTranslator
) {
  if (!value) {
    return null;
  }

  const trimmed = value.trim();

  return trimmed.length > 0
    ? `${translateLiteral(label)}: ${normalizeSentence(
        translateDiagnosticDetail(label, trimmed, translateLiteral)
      )}`
    : null;
}

function translateDiagnosticDetail(
  label: string,
  value: string,
  translateLiteral: DiagnosticTranslator
) {
  if (label === 'Field' || label === 'Expected') {
    return translateLiteral(value);
  }

  return value;
}

function formatDomainName(domain: string) {
  const trimmed = domain.trim();

  if (trimmed.length === 0) {
    return '';
  }

  const withoutPrefix = trimmed
    .replace(/^workflow[._-]/i, '')
    .replace(/^project[._-]/i, 'project ')
    .replace(/^desktop[._-]/i, 'desktop ')
    .replace(/^bridge[._-]/i, 'bridge ');

  return humanizeIdentifier(withoutPrefix);
}

function formatFieldName(field: string | null | undefined) {
  if (!field) {
    return null;
  }

  const trimmed = field.trim();

  if (trimmed.length === 0) {
    return null;
  }

  return humanizeIdentifier(trimmed);
}

function humanizeIdentifier(value: string) {
  const spaced = value
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[._/-]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();

  if (spaced.length === 0) {
    return value;
  }

  return spaced
    .split(' ')
    .map((part) => {
      if (/^[A-Z0-9]+$/.test(part)) {
        return part;
      }

      return part.charAt(0).toUpperCase() + part.slice(1);
    })
    .join(' ');
}

function normalizeSentence(value: string) {
  const trimmed = value.trim();

  if (trimmed.length === 0 || /[.!?]$/.test(trimmed)) {
    return trimmed;
  }

  return `${trimmed}.`;
}
