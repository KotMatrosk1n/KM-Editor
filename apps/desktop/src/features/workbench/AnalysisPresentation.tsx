/* SPDX-License-Identifier: GPL-3.0-only */

import type { ReactNode } from 'react';
import type { ApiDiagnostic } from '../../bridge/contracts';
import { useLocalization } from '../../localization';
import type { GroupedDiagnostic } from './analysisPresentationUtils';
import './analysisPresentation.css';

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
  const technical = diagnostics.filter(({ diagnostic }) => (
    diagnostics.length > 1 || diagnostic.code || diagnostic.domain || diagnostic.field
  ));
  if (technical.length === 0) return null;
  return (
    <TechnicalDetails summary={summary}>
      {technical.map(({ count, diagnostic, key }) => (
        <div className="km-analysis-diagnostic-identity" key={key}>
          <code>{`severity=${diagnostic.severity}`}</code>
          {diagnostic.code ? <code>{diagnostic.code}</code> : null}
          {diagnostic.domain ? <code>{diagnostic.domain}</code> : null}
          {diagnostic.field ? <code>{diagnostic.field}</code> : null}
          <OccurrenceCount count={count} />
        </div>
      ))}
    </TechnicalDetails>
  );
}
