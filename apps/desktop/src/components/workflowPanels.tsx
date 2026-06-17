/* SPDX-License-Identifier: GPL-3.0-only */

import { Activity, CheckCircle, ClipboardCheck } from 'lucide-react';
import { type ApiDiagnostic, type ApplyResult, type ChangePlan } from '../bridge/contracts';
import { formatDiagnosticMessage } from '../diagnostics';

export type WorkflowPanelOutput = {
  actionDiagnostics: ApiDiagnostic[];
  applyResult: ApplyResult | null;
  changePlan: ChangePlan | null;
};

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

export function WorkflowPanelOutputSections({
  output,
  scrollAfterEntries,
  workflowDiagnostics
}: {
  output: WorkflowPanelOutput;
  scrollAfterEntries?: number;
  workflowDiagnostics: ApiDiagnostic[];
}) {
  const combinedDiagnostics = [
    ...workflowDiagnostics,
    ...output.actionDiagnostics,
    ...(output.changePlan?.diagnostics ?? []),
    ...(output.applyResult?.diagnostics ?? [])
  ];

  return (
    <>
      {output.changePlan ? <ChangePlanSection changePlan={output.changePlan} /> : null}
      {output.applyResult ? <ApplyResultSection applyResult={output.applyResult} /> : null}
      <DiagnosticsSection
        diagnostics={combinedDiagnostics}
        scrollAfterEntries={scrollAfterEntries}
      />
    </>
  );
}

export function ChangePlanSection({ changePlan }: { changePlan: ChangePlan }) {
  return (
    <section aria-labelledby="change-plan-heading" className="panel wide-panel">
      <div className="panel-heading">
        <ClipboardCheck aria-hidden="true" size={18} />
        <h2 id="change-plan-heading">Output Plan</h2>
      </div>

      <div className="change-plan-status">
        <Metric label="Plan status" value={changePlan.canApply ? 'Ready' : 'Needs fixes'} />
        <Metric label="Session" value={changePlan.sessionId} />
      </div>

      {changePlan.writes.length > 0 ? (
        <ul className="change-plan-list">
          {changePlan.writes.map((write) => (
            <li key={write.targetRelativePath}>
              <div>
                <strong>{write.targetRelativePath}</strong>
                <span>{write.reason}</span>
              </div>
              <dl>
                <div>
                  <dt>Output state</dt>
                  <dd>
                    {write.replacesExistingOutput ? 'Replaces output file' : 'Creates output file'}
                  </dd>
                </div>
                <div>
                  <dt>Sources</dt>
                  <dd>
                    {write.sources
                      .map((source) => `${formatProjectFileLayer(source.layer)} ${source.relativePath}`)
                      .join(', ')}
                  </dd>
                </div>
              </dl>
            </li>
          ))}
        </ul>
      ) : (
        <p className="empty-copy">No target files in this plan.</p>
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

function formatProjectFileLayer(layer: ChangePlan['writes'][number]['sources'][number]['layer']) {
  return {
    base: 'Base',
    generated: 'Generated',
    layered: 'LayeredFS',
    pending: 'Pending'
  }[layer];
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
