/* SPDX-License-Identifier: GPL-3.0-only */

import type { ApiDiagnostic } from './bridge/contracts';

export function formatDiagnosticMessage(diagnostic: ApiDiagnostic) {
  const message = normalizeSentence(diagnostic.message);

  if (diagnostic.severity === 'info') {
    return message;
  }

  const valueDetails = [
    formatLabeledDetail('File', diagnostic.file),
    formatLabeledDetail('Field', formatFieldName(diagnostic.field)),
    formatLabeledDetail('Expected', diagnostic.expected)
  ].filter((detail): detail is string => detail !== null);

  if (valueDetails.length === 0) {
    return message;
  }

  const details = [
    formatDomainDetail(diagnostic.domain),
    ...valueDetails
  ].filter((detail): detail is string => detail !== null);

  return `${message} ${details.join(' ')}`;
}

function formatDomainDetail(domain: string | null | undefined) {
  if (!domain) {
    return null;
  }

  const readableDomain = formatDomainName(domain);

  return readableDomain ? `Area: ${readableDomain}.` : null;
}

function formatLabeledDetail(label: string, value: string | null | undefined) {
  if (!value) {
    return null;
  }

  const trimmed = value.trim();

  return trimmed.length > 0 ? `${label}: ${normalizeSentence(trimmed)}` : null;
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
