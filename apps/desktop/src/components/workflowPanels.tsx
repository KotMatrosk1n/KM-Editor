/* SPDX-License-Identifier: GPL-3.0-only */

import { Activity, CheckCircle } from 'lucide-react';
import { type ApiDiagnostic, type ApplyResult } from '../bridge/contracts';
import { formatDiagnosticMessage } from '../diagnostics';

export function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="metric">
      <span className="metric-label">{label}</span>
      <span className="metric-value metric-value-small">{value}</span>
    </div>
  );
}

export function ApplyResultSection({ applyResult }: { applyResult: ApplyResult }) {
  return (
    <section aria-labelledby="apply-result-heading" className="panel wide-panel">
      <div className="panel-heading">
        <CheckCircle aria-hidden="true" size={18} />
        <h2 id="apply-result-heading">Save Result</h2>
      </div>

      <div className="change-plan-status">
        <Metric label="Save ID" value={applyResult.applyId} />
        <Metric label="Written files" value={applyResult.writtenFiles.length.toString()} />
      </div>

      {applyResult.writtenFiles.length > 0 ? (
        <ul className="written-file-list">
          {applyResult.writtenFiles.map((writtenFile) => (
            <li key={writtenFile}>{writtenFile}</li>
          ))}
        </ul>
      ) : (
        <p className="empty-copy">No files were written.</p>
      )}
    </section>
  );
}

export function DiagnosticsSection({
  diagnostics,
  scrollAfterEntries
}: {
  diagnostics: ApiDiagnostic[];
  scrollAfterEntries?: number;
}) {
  const isScrollable = scrollAfterEntries !== undefined && diagnostics.length > scrollAfterEntries;

  return (
    <section aria-labelledby="diagnostics-heading" className="panel">
      <div className="panel-heading">
        <Activity aria-hidden="true" size={18} />
        <h2 id="diagnostics-heading">Diagnostics</h2>
      </div>

      {diagnostics.length > 0 ? (
        <ul className={`diagnostic-list ${isScrollable ? 'diagnostic-list-scrollable' : ''}`}>
          {diagnostics.map((diagnostic, index) => (
            <li
              className={`diagnostic diagnostic-${diagnostic.severity}`}
              key={`${diagnostic.severity}-${diagnostic.message}-${index}`}
            >
              <strong>{formatDiagnosticSeverity(diagnostic.severity)}</strong>
              <span>{formatDiagnosticMessage(diagnostic)}</span>
            </li>
          ))}
        </ul>
      ) : (
        <p className="empty-copy">No diagnostics.</p>
      )}
    </section>
  );
}

function formatDiagnosticSeverity(severity: ApiDiagnostic['severity']) {
  switch (severity) {
    case 'error':
      return 'Error';
    case 'warning':
      return 'Warning';
    case 'info':
      return 'Info';
  }
}
