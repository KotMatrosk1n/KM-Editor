/* SPDX-License-Identifier: GPL-3.0-only */

import { Activity, CheckCircle, ClipboardCheck } from 'lucide-react';
import { type ApiDiagnostic, type ApplyResult, type ChangePlan } from '../bridge/contracts';
import { formatDiagnosticMessage } from '../diagnostics';
import { useLocalization } from '../localization';

export type WorkflowPanelOutput = {
  actionDiagnostics: ApiDiagnostic[];
  applyResult: ApplyResult | null;
  changePlan: ChangePlan | null;
};

export function Metric({ label, value }: { label: string; value: string }) {
  const { translateLiteral } = useLocalization();

  return (
    <div className="metric">
      <span className="metric-label">{translateLiteral(label)}</span>
      <span className="metric-value metric-value-small">{translateLiteral(value)}</span>
    </div>
  );
}

export function ApplyResultSection({ applyResult }: { applyResult: ApplyResult }) {
  const { translateLiteral } = useLocalization();

  return (
    <section aria-labelledby="apply-result-heading" className="panel wide-panel">
      <div className="panel-heading">
        <CheckCircle aria-hidden="true" size={18} />
        <h2 id="apply-result-heading">{translateLiteral('Save Result')}</h2>
      </div>

      <div className="change-plan-status">
        <Metric label="Save ID" value={applyResult.applyId} />
        <Metric label="Written files" value={applyResult.writtenFiles.length.toString()} />
      </div>

      {applyResult.writtenFiles.length > 0 ? (
        <ul className="written-file-list">
          {applyResult.writtenFiles.map((writtenFile) => (
            <li data-localization-ignore="true" key={writtenFile}>
              {writtenFile}
            </li>
          ))}
        </ul>
      ) : (
        <p className="empty-copy">{translateLiteral('No files were written.')}</p>
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
  const { translateLiteral } = useLocalization();

  return (
    <section aria-labelledby="change-plan-heading" className="panel wide-panel">
      <div className="panel-heading">
        <ClipboardCheck aria-hidden="true" size={18} />
        <h2 id="change-plan-heading">{translateLiteral('Output Plan')}</h2>
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
                <strong data-localization-ignore="true">{write.targetRelativePath}</strong>
                <span>{translateLiteral(write.reason)}</span>
              </div>
              <dl>
                <div>
                  <dt>{translateLiteral('Output state')}</dt>
                  <dd>
                    {translateLiteral(
                      write.replacesExistingOutput ? 'Replaces output file' : 'Creates output file'
                    )}
                  </dd>
                </div>
                <div>
                  <dt>{translateLiteral('Sources')}</dt>
                  <dd>
                    {write.sources
                      .map(
                        (source) =>
                          `${translateLiteral(formatProjectFileLayer(source.layer))} ${source.relativePath}`
                      )
                      .join(', ')}
                  </dd>
                </div>
              </dl>
            </li>
          ))}
        </ul>
      ) : (
        <p className="empty-copy">{translateLiteral('No target files in this plan.')}</p>
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
  const { translateLiteral } = useLocalization();

  return (
    <section aria-labelledby="diagnostics-heading" className="panel">
      <div className="panel-heading">
        <Activity aria-hidden="true" size={18} />
        <h2 id="diagnostics-heading">{translateLiteral('Diagnostics')}</h2>
      </div>

      {diagnostics.length > 0 ? (
        <ul className={`diagnostic-list ${isScrollable ? 'diagnostic-list-scrollable' : ''}`}>
          {diagnostics.map((diagnostic, index) => (
            <li
              className={`diagnostic diagnostic-${diagnostic.severity}`}
              key={`${diagnostic.severity}-${diagnostic.message}-${index}`}
            >
              <strong>
                {translateLiteral(formatDiagnosticSeverity(diagnostic.severity))}
              </strong>
              <span>{formatDiagnosticMessage(diagnostic, translateLiteral)}</span>
            </li>
          ))}
        </ul>
      ) : (
        <p className="empty-copy">{translateLiteral('No diagnostics.')}</p>
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
