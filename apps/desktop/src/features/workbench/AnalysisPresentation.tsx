/* SPDX-License-Identifier: GPL-3.0-only */

import type { ReactNode } from 'react';
import type { ApiDiagnostic } from '../../bridge/contracts';
import { sanitizeReportableErrorText } from '../../errorReporting';
import { useLocalization } from '../../localization';
import {
  humanizeIdentifier,
  type GroupedDiagnostic
} from './analysisPresentationUtils';
import './analysisPresentation.css';

const maximumDisplayedDiagnosticIdentities = 3;
const maximumDiagnosticSourceUnits = 2048;
const maximumDiagnosticValueCharacters = 160;

export function OccurrenceCount({ count }: { count: number }) {
  const { t } = useLocalization();
  if (count < 2) return null;
  return (
    <span className="km-analysis-occurrence-count">
      {t('analysisPresentation.occurrences', { count })}
    </span>
  );
}

export function TechnicalDetails({
  children,
  summary
}: {
  children: ReactNode;
  summary: string;
}) {
  return (
    <details className="km-analysis-technical-details">
      <summary>{summary}</summary>
      <div data-localization-ignore="true">{children}</div>
    </details>
  );
}

export function DiagnosticTechnicalDetails({
  diagnostics,
  summary
}: {
  diagnostics: readonly GroupedDiagnostic<ApiDiagnostic>[];
  summary: string;
}) {
  const { t, translateLiteral } = useLocalization();
  const identities = presentDiagnosticIdentities(diagnostics, translateLiteral);
  if (identities.length === 0) return null;
  const displayed = identities.slice(0, maximumDisplayedDiagnosticIdentities);
  const remaining = identities.length - displayed.length;

  return (
    <TechnicalDetails summary={summary}>
      <ul className="km-analysis-diagnostic-identities">
        {displayed.map((identity) => (
          <li key={identity.key}>
            <dl className="km-analysis-diagnostic-details">
              {identity.code ? (
                <DiagnosticDetail
                  code
                  label={t('analysisPresentation.controls.identifier')}
                  value={identity.code}
                />
              ) : null}
              {identity.area ? (
                <DiagnosticDetail label={translateLiteral('Area')} value={identity.area} />
              ) : null}
              {identity.field ? (
                <DiagnosticDetail
                  label={t('analysisPresentation.controls.field')}
                  value={identity.field}
                />
              ) : null}
              {identity.file ? (
                <DiagnosticDetail code label={translateLiteral('File')} value={identity.file} />
              ) : null}
              {identity.expected ? (
                <DiagnosticDetail
                  label={translateLiteral('Expected')}
                  value={identity.expected}
                />
              ) : null}
            </dl>
            <OccurrenceCount count={identity.count} />
          </li>
        ))}
      </ul>
      {remaining > 0 ? (
        <p className="km-analysis-additional-identities">
          {t('analysisPresentation.additionalDiagnosticIdentities', { count: remaining })}
        </p>
      ) : null}
    </TechnicalDetails>
  );
}

export function DiagnosticSeverityText({
  severity
}: {
  severity: ApiDiagnostic['severity'];
}) {
  const { translateLiteral } = useLocalization();
  const label = {
    error: 'Error',
    info: 'Info',
    warning: 'Warning'
  }[severity];
  return (
    <span className="km-analysis-visually-hidden">
      {translateLiteral(label)}:{' '}
    </span>
  );
}

function DiagnosticDetail({
  code = false,
  label,
  value
}: {
  code?: boolean;
  label: string;
  value: string;
}) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{code ? <code>{value}</code> : value}</dd>
    </div>
  );
}

type PresentedDiagnosticIdentity = {
  area: string | null;
  code: string | null;
  count: number;
  expected: string | null;
  field: string | null;
  file: string | null;
  key: string;
};

function presentDiagnosticIdentities(
  diagnostics: readonly GroupedDiagnostic<ApiDiagnostic>[],
  translateLiteral: (literal: string) => string
) {
  const candidates = diagnostics.map(({ count, diagnostic }) => ({
    area: diagnostic.domain ? formatDiagnosticArea(diagnostic.domain, translateLiteral) : null,
    code: boundedDiagnosticValue(diagnostic.code),
    count,
    expected: diagnostic.expected
      ? boundedDiagnosticValue(translateLiteral(diagnosticSourcePrefix(diagnostic.expected)))
      : null,
    field: diagnostic.field
      ? formatDiagnosticIdentifier(diagnostic.field, translateLiteral)
      : null,
    file: boundedDiagnosticFile(diagnostic.file)
  }));
  const distinctAreas = new Set(
    candidates.map(({ area }) => area).filter((area): area is string => area !== null)
  );
  const grouped = new Map<string, PresentedDiagnosticIdentity>();

  for (const candidate of candidates) {
    const hasUsefulDetail = Boolean(
      candidate.code || candidate.expected || candidate.field || candidate.file
    );
    if (!hasUsefulDetail && !(candidate.area && distinctAreas.size > 1)) continue;
    const key = JSON.stringify([
      candidate.code,
      candidate.area,
      candidate.field,
      candidate.file,
      candidate.expected
    ]);
    const existing = grouped.get(key);
    if (existing) existing.count += candidate.count;
    else grouped.set(key, { ...candidate, key });
  }

  return [...grouped.values()];
}

function boundedDiagnosticValue(value: string | null | undefined) {
  if (!value) return null;
  const source = diagnosticSourcePrefix(value);
  if (
    /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f\u200b-\u200f\u202a-\u202e\u2060-\u206f\ufeff]/u
      .test(source)
  ) return null;
  const normalized = sanitizeReportableErrorText(source).replace(/\s+/gu, ' ').trim();
  if (!normalized || normalized.includes('[local path]')) return null;
  const characters = [...normalized];
  return characters.length <= maximumDiagnosticValueCharacters
    ? normalized
    : `${characters.slice(0, maximumDiagnosticValueCharacters - 3).join('')}...`;
}

function formatDiagnosticArea(
  value: string,
  translateLiteral: (literal: string) => string
) {
  const safeValue = boundedDiagnosticValue(value);
  if (!safeValue) return null;
  const normalized = safeValue
    .replace(/^workflow[._:/-]+/iu, '')
    .replace(/^project[._:/-]+/iu, 'project ')
    .replace(/^desktop[._:/-]+/iu, 'desktop ')
    .replace(/^bridge[._:/-]+/iu, 'bridge ');
  return boundedDiagnosticValue(
    translateLiteral(humanizeIdentifier(normalized || safeValue))
  );
}

function formatDiagnosticIdentifier(
  value: string,
  translateLiteral: (literal: string) => string
) {
  const safeValue = boundedDiagnosticValue(value);
  return safeValue
    ? boundedDiagnosticValue(translateLiteral(humanizeIdentifier(safeValue)))
    : null;
}

function boundedDiagnosticFile(value: string | null | undefined) {
  if (!value) return null;
  const source = diagnosticSourcePrefix(value);
  if (/[\u0000-\u001f\u007f]/u.test(source)) return null;
  const safeValue = sanitizeReportableErrorText(source).replace(/\s+/gu, ' ').trim();
  if (!safeValue || safeValue.includes('[local path]')) return null;
  const normalized = safeValue.replace(/\\/gu, '/');
  const segments = normalized.split('/');
  if (
    normalized.startsWith('/') ||
    /^[A-Za-z]:/u.test(normalized) ||
    normalized.includes(':') ||
    segments.some((segment) => !segment || segment === '.' || segment === '..')
  ) return null;
  return boundedDiagnosticValue(normalized);
}

function diagnosticSourcePrefix(value: string) {
  const prefix = value.slice(0, maximumDiagnosticSourceUnits);
  const finalCodeUnit = prefix.charCodeAt(prefix.length - 1);
  return finalCodeUnit >= 0xd800 && finalCodeUnit <= 0xdbff
    ? prefix.slice(0, -1)
    : prefix;
}
